@L2 @group-pre-order
Feature: Organizer sends the group pre-order to the restaurant

  Once everyone has chosen, the organizer sends one consolidated pre-order. Only carts
  the guests confirmed are taken; identical dishes from different people become one
  line with the combined quantity; the organizer's own dishes are part of it. The
  restaurant's order is then waiting for the kitchen and the group cannot be changed
  any more. The waiter serving the table sees exactly what was sent.

  Background:
    Given a restaurant with these dishes on its menu
      | dish         | price |
      | Caesar Salad | 12    |
      | Tom Yum      | 20    |
      | Cheesecake   | 8     |
    And the organizer has booked a table for 6 guests

  Rule: Sending merges the confirmed carts into one pre-order

    Background:
      Given the organizer has pre-ordered for themselves
        | dish    | quantity |
        | Tom Yum | 1        |
      And "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 2        |
      And "Ivan" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 1        |
        | Tom Yum      | 1        |
      And "Maksym" has joined the group and is still choosing, with
        | dish       | quantity |
        | Cheesecake | 2        |

    @ui @smoke
    Scenario: The organizer sends the pre-order from the group page
      When the organizer opens the group pre-order
      And the organizer sends the group pre-order to the restaurant
      Then the organizer sees that the pre-order was sent to the restaurant
      And the pre-order sent to the restaurant is
        | dish         | quantity |
        | Caesar Salad | 3        |
        | Tom Yum      | 2        |

    @api @smoke
    Scenario: The restaurant receives one order with identical dishes merged
      When the organizer sends the group pre-order to the restaurant
      Then the pre-order sent to the restaurant is
        | dish         | quantity |
        | Caesar Salad | 3        |
        | Tom Yum      | 2        |
      And the restaurant's order is waiting to be confirmed by the kitchen
      And the group pre-order is closed for changes

  Rule: The waiter of the table sees what the organizer sent

    Background:
      Given the organizer has pre-ordered for themselves
        | dish    | quantity |
        | Tom Yum | 1        |
      And "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 2        |
      And "Maksym" has joined the group and is still choosing, with
        | dish       | quantity |
        | Cheesecake | 2        |
      And the organizer has sent the group pre-order to the restaurant

    @ui
    Scenario: The waiter opens the pre-order of the reservation
      When the waiter opens the pre-order of the reservation
      Then the waiter sees these dishes
        | dish         | quantity |
        | Caesar Salad | 2        |
        | Tom Yum      | 1        |

    @api
    Scenario: The waiter's copy of the order has the confirmed dishes only
      When the waiter opens the pre-order of the reservation
      Then the waiter sees these dishes
        | dish         | quantity |
        | Caesar Salad | 2        |
        | Tom Yum      | 1        |

  Rule: A pre-order without confirmed dishes cannot be sent

    @api @negative
    Scenario: Nobody has confirmed a cart
      Given "Anna" has joined the group and is still choosing, with
        | dish         | quantity |
        | Caesar Salad | 1        |
      When the organizer tries to send the group pre-order to the restaurant
      Then the organizer is told "There are no submitted guest sessions in this group reservation."

    @api @negative
    Scenario: The confirmed carts hold no dishes
      Given "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 1        |
      And the organizer has taken "Caesar Salad" out of the cart of "Anna"
      When the organizer tries to send the group pre-order to the restaurant
      Then the organizer is told "Submitted guest sessions do not contain any dishes."

    @api @negative
    Scenario: The reservation has no order to add the dishes to
      Given "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 1        |
      When the organizer tries to send the group pre-order to the restaurant
      Then the organizer is told that the reservation has no order to send

    @api @negative
    Scenario: The reservation named in the request does not exist
      Given "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 1        |
      When the organizer tries to send the group pre-order for a reservation that does not exist
      Then the organizer is told "Reservation does not belong to this group reservation."

    @api @negative
    Scenario: The group does not exist
      When the organizer tries to send a group pre-order that does not exist
      Then the organizer is told "Group reservation was not found"

    @ui @negative
    Scenario: The page does not let the organizer send a pre-order nobody confirmed
      Given "Anna" has joined the group and is still choosing, with
        | dish         | quantity |
        | Caesar Salad | 1        |
      When the organizer opens the group pre-order
      Then the organizer cannot send the pre-order yet
      And the organizer is told that nothing will be sent to the restaurant

  Rule: A group pre-order is sent only once, and only by the organizer

    Background:
      Given the organizer has pre-ordered for themselves
        | dish    | quantity |
        | Tom Yum | 1        |
      And "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 1        |

    @api @negative
    Scenario: Sending a group pre-order a second time is refused
      Given the organizer has sent the group pre-order to the restaurant
      When the organizer tries to send the group pre-order to the restaurant
      Then the organizer is told "Group reservation was submitted."
      And the pre-order sent to the restaurant is
        | dish         | quantity |
        | Caesar Salad | 1        |
        | Tom Yum      | 1        |

    @api @negative
    Scenario Outline: Someone signed in who is not the organizer cannot send it
      When <someone> tries to send the group pre-order to the restaurant
      Then they are told that only the organizer can do this
      And the group pre-order is still open

      Examples: Signed-in people who are not the organizer
        | someone          |
        | another customer |
        | the waiter       |

    @api @negative
    Scenario: Nobody can send it without signing in
      When an anonymous visitor tries to send the group pre-order to the restaurant
      Then they are asked to sign in first
      And the group pre-order is still open
